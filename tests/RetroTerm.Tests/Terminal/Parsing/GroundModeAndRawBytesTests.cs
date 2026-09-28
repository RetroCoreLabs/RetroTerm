using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Parsing;
using Xunit;

namespace RetroTerm.Tests.Terminal.Parsing;

/// <summary>
/// Phase 1 parser hardening: the seam for terminal modes whose bytes are not ANSI text.
///
/// Three planned terminals need this and none of them could be expressed before:
///   VT52       - "ESC Y row col" carries two binary coordinate bytes;
///   Tektronix  - after GS, plain bytes are vector coordinates, not text;
///   ND / TDV   - "DLE row col" cursor addressing, today filtered out ahead of
///                the parser by TDVInputProcessor, which is why the parser cannot see
///                terminal modes and modes cannot see the parser.
///
/// Two facilities cover all three: ExpectRawBytes for fixed-length binary parameters, and
/// GroundFilter for "while this mode is on, plain bytes mean something else". These tests
/// exercise the mechanism using those real shapes; the emulators themselves come later.
/// </summary>
public class GroundModeAndRawBytesTests
{
    private static void Feed(EscapeSequenceParser parser, string s)
    {
        parser.ProcessBytes(Encoding.ASCII.GetBytes(s));
    }

    // ─────────────────────────────────────────────────────────────
    // ExpectRawBytes - binary parameters after a sequence
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Vt52StyleEscY_CoordinatesAreCollected_NotPrintedAsText()
    {
        // ESC Y <row><col>, VT52 cursor addressing. Without raw collection the two
        // coordinate bytes fell through to the screen as characters.
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        byte[]? coords = null;

        parser.OnCharacter += cp => printed.Append((char)cp);
        parser.OnEscapeDispatch += p =>
        {
            if (p.FinalByte == (byte)'Y')
            {
                p.ExpectRawBytes(2, bytes => coords = bytes.ToArray());
            }
        };

        // ESC Y then 0x20+row, 0x20+col (row 5, col 10), then ordinary text
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'Y', 0x25, 0x2A });
        Feed(parser, "HI");

        Assert.NotNull(coords);
        Assert.Equal(new byte[] { 0x25, 0x2A }, coords);
        Assert.Equal("HI", printed.ToString());
    }

    [Fact]
    public void RawBytes_AreTakenVerbatim_EvenWhenTheyLookLikeControls()
    {
        // The whole point: a binary coordinate may be 0x1B or 0x07 and must not be
        // interpreted. This is why raw collection runs ahead of ESC handling.
        var parser = new EscapeSequenceParser();
        var executed = new List<byte>();
        byte[]? raw = null;

        parser.OnExecute += b => executed.Add(b);
        parser.OnEscapeDispatch += p =>
        {
            if (p.FinalByte == (byte)'Y')
            {
                p.ExpectRawBytes(2, bytes => raw = bytes.ToArray());
            }
        };

        parser.ProcessBytes(new byte[] { 0x1B, (byte)'Y', 0x1B, 0x07 });

        Assert.Equal(new byte[] { 0x1B, 0x07 }, raw);
        Assert.Empty(executed);
    }

    [Fact]
    public void RawBytes_SplitAcrossReads_AreReassembled()
    {
        var parser = new EscapeSequenceParser();
        byte[]? raw = null;
        parser.OnEscapeDispatch += p =>
        {
            if (p.FinalByte == (byte)'Y') p.ExpectRawBytes(2, b => raw = b.ToArray());
        };

        parser.ProcessBytes(new byte[] { 0x1B, (byte)'Y' });
        Assert.True(parser.IsCollectingRawBytes);
        parser.ProcessBytes(new byte[] { 0x41 });
        Assert.True(parser.IsCollectingRawBytes);
        parser.ProcessBytes(new byte[] { 0x42 });

        Assert.False(parser.IsCollectingRawBytes);
        Assert.Equal(new byte[] { 0x41, 0x42 }, raw);
    }

    [Fact]
    public void DleStyleCursorAddressing_WorksFromAnExecuteHandler()
    {
        // The ND/TDV shape: a C0 control introduces two binary coordinate bytes. Today
        // this is done by filtering the stream before the parser ever sees it.
        const byte DLE = 0x10;
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        byte[]? coords = null;

        parser.OnCharacter += cp => printed.Append((char)cp);
        parser.OnExecute += b =>
        {
            if (b == DLE)
            {
                parser.ExpectRawBytes(2, bytes => coords = bytes.ToArray());
            }
        };

        parser.ProcessBytes(new byte[] { DLE, 0x30, 0x41 });
        Feed(parser, "OK");

        Assert.Equal(new byte[] { 0x30, 0x41 }, coords);
        Assert.Equal("OK", printed.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EscapeSequenceParser.MaxRawBytes + 1)]
    public void ExpectRawBytes_RejectsSillyCounts(int count)
    {
        var parser = new EscapeSequenceParser();
        Assert.Throws<ArgumentOutOfRangeException>(() => parser.ExpectRawBytes(count, _ => { }));
    }

    [Fact]
    public void ExpectRawBytes_RejectsNullHandler()
    {
        var parser = new EscapeSequenceParser();
        Assert.Throws<ArgumentNullException>(() => parser.ExpectRawBytes(2, null!));
    }

    [Fact]
    public void Reset_AbandonsAnUnfinishedRawRequest()
    {
        var parser = new EscapeSequenceParser();
        var called = false;
        parser.ExpectRawBytes(2, _ => called = true);

        parser.Reset();
        parser.ProcessBytes(new byte[] { 0x41, 0x42 });

        Assert.False(called, "a raw request should not survive a parser reset");
        Assert.False(parser.IsCollectingRawBytes);
    }

    // ─────────────────────────────────────────────────────────────
    // GroundFilter - alternate meaning for plain bytes
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GroundFilter_ConsumesBytesInsteadOfPrintingThem()
    {
        // Tektronix shape: after GS, plain bytes are vector coordinates.
        const byte GS = 0x1D;
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        var vectorBytes = new List<byte>();
        var inVectorMode = false;

        parser.OnCharacter += cp => printed.Append((char)cp);
        parser.OnExecute += b => { if (b == GS) inVectorMode = true; };
        parser.GroundFilter = b =>
        {
            if (!inVectorMode) return false;
            if (b == 0x1F) { inVectorMode = false; return true; } // US leaves vector mode
            vectorBytes.Add(b);
            return true;
        };

        Feed(parser, "TEXT");
        parser.ProcessBytes(new byte[] { GS, 0x20, 0x40, 0x60, 0x7E, 0x1F });
        Feed(parser, "MORE");

        Assert.Equal(new byte[] { 0x20, 0x40, 0x60, 0x7E }, vectorBytes.ToArray());
        Assert.Equal("TEXTMORE", printed.ToString());
    }

    [Fact]
    public void GroundFilter_CanDeclineAndLetTheParserProceed()
    {
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        var seen = new List<byte>();

        parser.OnCharacter += cp => printed.Append((char)cp);
        parser.GroundFilter = b => { seen.Add(b); return false; }; // observe only

        Feed(parser, "AB");

        Assert.Equal(new byte[] { (byte)'A', (byte)'B' }, seen.ToArray());
        Assert.Equal("AB", printed.ToString());
    }

    [Fact]
    public void GroundFilter_NeverSeesEscapeSequenceBytes_NotEvenTheEsc()
    {
        // Two properties in one, both deliberate:
        //  - the filter must not interfere with a partly parsed sequence, or a terminal
        //    mode could corrupt ordinary CSI handling;
        //  - it does not even get the ESC, because ESC is handled before the Ground
        //    fast-path. That is what guarantees a mode can always be escaped: a filter
        //    that swallowed ESC could wedge the terminal in its own mode forever.
        var parser = new EscapeSequenceParser();
        var seen = new List<byte>();
        char final = '\0';

        parser.GroundFilter = b => { seen.Add(b); return false; };
        parser.OnCsiDispatch += p => final = (char)p.FinalByte;

        Feed(parser, "\x1b[31mX");

        Assert.Equal('m', final);
        Assert.Equal(new byte[] { (byte)'X' }, seen.ToArray());
    }

    [Fact]
    public void EscapeSequencesStillWork_WhileAGroundModeIsActive()
    {
        // Follows from the above and matters in practice: a Tektronix-style mode must not
        // stop the host switching the terminal back with an escape sequence.
        var parser = new EscapeSequenceParser();
        var consumed = new List<byte>();
        char final = '\0';

        parser.GroundFilter = b => { consumed.Add(b); return true; }; // swallow everything
        parser.OnCsiDispatch += p => final = (char)p.FinalByte;

        Feed(parser, "AB\x1b[0mCD");

        Assert.Equal('m', final);
        Assert.Equal(new byte[] { (byte)'A', (byte)'B', (byte)'C', (byte)'D' }, consumed.ToArray());
    }

    [Fact]
    public void GroundFilter_SurvivesAParserReset()
    {
        // A reset (RIS, reconnect) must not silently switch off a terminal mode the
        // emulator believes is still on.
        var parser = new EscapeSequenceParser();
        var consumed = 0;
        parser.GroundFilter = _ => { consumed++; return true; };

        Feed(parser, "A");
        parser.Reset();
        Feed(parser, "B");

        Assert.Equal(2, consumed);
        Assert.NotNull(parser.GroundFilter);
    }

    [Fact]
    public void ClearingGroundFilter_RestoresNormalTextHandling()
    {
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        parser.OnCharacter += cp => printed.Append((char)cp);
        parser.GroundFilter = _ => true;

        Feed(parser, "HIDDEN");
        parser.GroundFilter = null;
        Feed(parser, "SHOWN");

        Assert.Equal("SHOWN", printed.ToString());
    }
}
