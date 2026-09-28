using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Parsing;
using Xunit;

namespace RetroTerm.Tests.Terminal.Parsing;

/// <summary>
/// Phase 1 parser hardening: DCS and string-sequence handling.
///
/// Before this, DCS was a stub that made Sixel, ReGIS and DECUDK impossible:
///  - parameters were thrown away and there was no DcsParam case at all, so any
///    parameterised DCS (e.g. "DCS 0;1;0 q") wedged the parser until the next ESC;
///  - payload bytes were dropped on the floor and OnDcsPut was never raised;
///  - only a bare 0x9C could end the sequence, while hosts send "ESC \" - and the
///    parser's global "ESC always restarts" rule ate that ESC, so ST never worked.
///
/// These tests pin all three down, plus sub-parameter support (SGR 38:2::R:G:B).
/// </summary>
public class DcsAndStringSequenceTests
{
    /// <summary>
    /// Captures everything a DCS produces, so a test can assert on the whole stream.
    /// </summary>
    private sealed class DcsRecorder
    {
        public readonly List<string> Hooks = new();
        public readonly List<int[]> HookParameters = new();
        public readonly StringBuilder Payload = new();
        public int PutCallCount;
        public int UnhookCount;

        public DcsRecorder(EscapeSequenceParser parser)
        {
            parser.OnDcsHook += p =>
            {
                Hooks.Add(((char)p.FinalByte).ToString());
                HookParameters.Add(p.Parameters.ToArray());
            };
            parser.OnDcsPut += data =>
            {
                PutCallCount++;
                for (int i = 0; i < data.Length; i++)
                {
                    Payload.Append((char)data[i]);
                }
            };
            parser.OnDcsUnhook += () => UnhookCount++;
        }
    }

    private static void Feed(EscapeSequenceParser parser, string s)
    {
        parser.ProcessBytes(Encoding.ASCII.GetBytes(s));
    }

    // ─────────────────────────────────────────────────────────────
    // DCS introducer: parameters must survive
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ParameterlessDcs_Hooks()
    {
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        Feed(parser, "\x1bPq\x1b\\");

        Assert.Single(rec.Hooks);
        Assert.Equal("q", rec.Hooks[0]);
        Assert.Equal(1, rec.UnhookCount);
    }

    [Fact]
    public void ParameterisedDcs_HooksAndKeepsItsParameters()
    {
        // The exact shape a Sixel image arrives with: DCS P1;P2;P3 q ... ST
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        Feed(parser, "\x1bP0;1;0q\x1b\\");

        Assert.Single(rec.Hooks);
        Assert.Equal("q", rec.Hooks[0]);
        Assert.Equal(new[] { 0, 1, 0 }, rec.HookParameters[0]);
        Assert.Equal(1, rec.UnhookCount);
    }

    [Fact]
    public void ParameterisedDcs_DoesNotWedgeTheParser()
    {
        // The old parser had no DcsParam case, so after the first digit every byte matched
        // nothing and the parser was stuck - text after the sequence vanished.
        var parser = new EscapeSequenceParser();
        var characters = new StringBuilder();
        parser.OnCharacter += cp => characters.Append((char)cp);

        Feed(parser, "\x1bP0;1;0q~~~\x1b\\AFTER");

        Assert.Equal("AFTER", characters.ToString());
    }

    [Fact]
    public void DcsWithIntermediate_Hooks()
    {
        // DECUDK-style: DCS <params> | ... ST, and DCS with an intermediate byte
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        Feed(parser, "\x1bP1;1|17/41\x1b\\");

        Assert.Single(rec.Hooks);
        Assert.Equal("|", rec.Hooks[0]);
        Assert.Equal(new[] { 1, 1 }, rec.HookParameters[0]);
        Assert.Equal("17/41", rec.Payload.ToString());
    }

    // ─────────────────────────────────────────────────────────────
    // Payload streaming
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DcsPayload_IsDeliveredToOnDcsPut()
    {
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        Feed(parser, "\x1bPq#0;2;0;0;0#1;2;100;100;100\x1b\\");

        Assert.Equal("#0;2;0;0;0#1;2;100;100;100", rec.Payload.ToString());
        Assert.Equal(1, rec.UnhookCount);
    }

    [Fact]
    public void LargeDcsPayload_IsStreamedInChunks_NotBufferedWhole()
    {
        // A real Sixel image is far larger than the chunk size; the point of streaming is
        // that the consumer sees it in pieces rather than the parser holding it all.
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        var payload = new string('?', 10000);
        Feed(parser, "\x1bPq" + payload + "\x1b\\");

        Assert.Equal(10000, rec.Payload.Length);
        Assert.True(rec.PutCallCount > 1,
            $"payload should arrive in multiple chunks, got {rec.PutCallCount} call(s)");
        Assert.Equal(1, rec.UnhookCount);
    }

    [Fact]
    public void DcsPayload_SplitAcrossReads_IsReassembled()
    {
        // Network data arrives in arbitrary pieces; the sequence must survive being cut
        // anywhere, including between the ESC and the '\' of ST.
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        Feed(parser, "\x1bPq#1");
        Feed(parser, "23");
        Feed(parser, "\x1b");
        Feed(parser, "\\");

        Assert.Equal("#123", rec.Payload.ToString());
        Assert.Equal(1, rec.UnhookCount);
    }

    // ─────────────────────────────────────────────────────────────
    // Terminators
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EscBackslash_TerminatesDcs()
    {
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        Feed(parser, "\x1bPqDATA\x1b\\");

        Assert.Equal(1, rec.UnhookCount);
        Assert.Equal("DATA", rec.Payload.ToString());
    }

    [Fact]
    public void EightBitSt_TerminatesDcs()
    {
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        parser.ProcessBytes(new byte[] { 0x1B, (byte)'P', (byte)'q', (byte)'D', 0x9C });

        Assert.Equal(1, rec.UnhookCount);
        Assert.Equal("D", rec.Payload.ToString());
    }

    [Fact]
    public void EscBackslash_TerminatesOsc()
    {
        var parser = new EscapeSequenceParser();
        string? osc = null;
        parser.OnOscDispatch += d => osc = Encoding.ASCII.GetString(d.ToArray());

        Feed(parser, "\x1b]0;My Title\x1b\\");

        Assert.Equal("0;My Title", osc);
    }

    [Fact]
    public void BellStillTerminatesOsc()
    {
        // The xterm-style BEL terminator must keep working alongside ST.
        var parser = new EscapeSequenceParser();
        string? osc = null;
        parser.OnOscDispatch += d => osc = Encoding.ASCII.GetString(d.ToArray());

        Feed(parser, "\x1b]0;Title\x07");

        Assert.Equal("0;Title", osc);
    }

    [Fact]
    public void EscInsideOscThatIsNotSt_AbandonsTheString()
    {
        // A stray ESC mid-string means the host gave up. The string must be dropped, not
        // dispatched, and the parser must recover to handle what follows.
        var parser = new EscapeSequenceParser();
        var oscCount = 0;
        var characters = new StringBuilder();
        parser.OnOscDispatch += _ => oscCount++;
        parser.OnCharacter += cp => characters.Append((char)cp);

        Feed(parser, "\x1b]0;Broken\x1b[1mAFTER");

        Assert.Equal(0, oscCount);
        Assert.Equal("AFTER", characters.ToString());
    }

    [Fact]
    public void EscInsideDcsThatIsNotSt_EndsTheSequence()
    {
        // The hook already fired, so the consumer must still be told the DCS ended -
        // otherwise it waits for payload forever.
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);
        var characters = new StringBuilder();
        parser.OnCharacter += cp => characters.Append((char)cp);

        Feed(parser, "\x1bPqDATA\x1b[1mAFTER");

        Assert.Single(rec.Hooks);
        Assert.Equal(1, rec.UnhookCount);
        Assert.Equal("AFTER", characters.ToString());
    }

    [Fact]
    public void TwoDcsSequencesInARow_BothComplete()
    {
        var parser = new EscapeSequenceParser();
        var rec = new DcsRecorder(parser);

        Feed(parser, "\x1bPqFIRST\x1b\\\x1bPqSECOND\x1b\\");

        Assert.Equal(2, rec.Hooks.Count);
        Assert.Equal(2, rec.UnhookCount);
        Assert.Equal("FIRSTSECOND", rec.Payload.ToString());
    }

    // ─────────────────────────────────────────────────────────────
    // Sub-parameters (colon), used by modern SGR
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ColonSubParameters_AreParsed_NotDiscarded()
    {
        // ':' used to match nothing and silently killed the sequence, so the ITU-T T.416
        // colour form that modern terminals emit was dropped entirely.
        var parser = new EscapeSequenceParser();
        int[]? parameters = null;
        char final = '\0';
        parser.OnCsiDispatch += p => { parameters = p.Parameters.ToArray(); final = (char)p.FinalByte; };

        Feed(parser, "\x1b[38:2:255:0:0m");

        Assert.Equal('m', final);
        Assert.Equal(new[] { 38, 2, 255, 0, 0 }, parameters);
    }

    [Fact]
    public void SubParameters_AreFlaggedAsSuch()
    {
        var parser = new EscapeSequenceParser();
        bool[]? flags = null;
        parser.OnCsiDispatch += p => flags = p.SubParameterFlags.ToArray();

        Feed(parser, "\x1b[38:2:255;5m");

        // 38 is a top-level parameter; 2, 255 follow colons; 5 follows a semicolon.
        Assert.Equal(new[] { false, true, true, false }, flags);
    }

    [Fact]
    public void SemicolonForm_StillHasNoSubParameters()
    {
        var parser = new EscapeSequenceParser();
        bool[]? flags = null;
        int[]? parameters = null;
        parser.OnCsiDispatch += p => { flags = p.SubParameterFlags.ToArray(); parameters = p.Parameters.ToArray(); };

        Feed(parser, "\x1b[38;2;255;0;0m");

        Assert.Equal(new[] { 38, 2, 255, 0, 0 }, parameters);
        Assert.Equal(new[] { false, false, false, false, false }, flags);
    }

    [Fact]
    public void CsiSequencesStillWorkNormally()
    {
        // Guard against the parameter refactor breaking ordinary sequences.
        var parser = new EscapeSequenceParser();
        int[]? parameters = null;
        char final = '\0';
        byte marker = 0;
        parser.OnCsiDispatch += p =>
        {
            parameters = p.Parameters.ToArray();
            final = (char)p.FinalByte;
            marker = p.PrivateMarker;
        };

        Feed(parser, "\x1b[?25h");

        Assert.Equal('h', final);
        Assert.Equal((byte)'?', marker);
        Assert.Equal(new[] { 25 }, parameters);
    }
}
