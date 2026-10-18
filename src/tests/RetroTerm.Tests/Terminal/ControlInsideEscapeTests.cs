using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Parsing;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// A C0 control arriving straight after ESC: it executes, and it ENDS the sequence.
///
/// THIS FILE RECORDS A CHANGE THAT WAS MADE AND THEN REVERTED, because the reasoning is worth
/// keeping.
///
/// The problem it started from is real: <c>ESC ENQ</c> is the standard Tektronix identification
/// request, and a bare <c>ENQ</c> on a TDV lights a keyboard lamp. Two commands, one byte. The fix
/// adopted was to follow ECMA-48 literally — a C0 arriving mid-sequence executes and the sequence
/// CONTINUES — so the ESC was still "in flight" when the ENQ ran.
///
/// That was wrong for the terminals this program emulates, and real third-party data proved it.
/// Every gnuplot Tektronix stream opens with <c>ESC FF</c>, the Tek ERASE SCREEN command, where the
/// C0 IS the end of the command. Staying in Escape meant the <c>GS</c> and coordinate bytes that
/// followed were swallowed as intermediates and a final, and the first vector of every plot
/// disappeared.
///
/// The evidence was available before the data was. Breaking the change on purpose showed the
/// ESC ENQ test passing either way — because <c>State</c> is not updated until after
/// <c>OnExecute</c> returns, so a handler reading it during the callback sees Escape regardless.
/// The distinction that motivated the change already existed; ending the sequence costs nothing and
/// keeps <c>ESC FF</c> working.
/// </summary>
public class ControlInsideEscapeTests
{
    private sealed class Capture
    {
        public readonly List<byte> Executed = new List<byte>();
        public readonly List<ParserState> StateDuringExecute = new List<ParserState>();
        public readonly List<char> CsiFinals = new List<char>();
        public readonly StringBuilder Printed = new StringBuilder();
        public ParserState FinalState;
    }

    private static Capture Feed(string text)
    {
        var parser = new EscapeSequenceParser();
        var capture = new Capture();

        parser.OnExecute += b =>
        {
            capture.Executed.Add(b);

            // Read DURING the callback. This is the seam the identification reply depends on: the
            // state has not been updated yet, so it still says how this byte arrived.
            capture.StateDuringExecute.Add(parser.State);
        };
        parser.OnCsiDispatch += p => capture.CsiFinals.Add((char)p.FinalByte);
        parser.OnCharacter += cp => capture.Printed.Append((char)cp);

        parser.ProcessBytes(Encoding.ASCII.GetBytes(text));
        capture.FinalState = parser.State;
        return capture;
    }

    [Fact]
    public void AnEnqAfterEscIsDistinguishableFromABareOne()
    {
        // What the whole question was about, and it works without the sequence staying open.
        var afterEscape = Feed("\u001b\u0005");
        var onItsOwn = Feed("\u0005");

        Assert.Equal(new[] { (byte)0x05 }, afterEscape.Executed);
        Assert.Equal(ParserState.Escape, afterEscape.StateDuringExecute[0]);

        Assert.Equal(new[] { (byte)0x05 }, onItsOwn.Executed);
        Assert.Equal(ParserState.Ground, onItsOwn.StateDuringExecute[0]);
    }

    [Fact]
    public void TheControlStillExecutes()
    {
        // Not swallowed. A host is entitled to send a carriage return after an ESC and expect the
        // carriage to return.
        var capture = Feed("\u001b\r");

        Assert.Contains((byte)0x0D, capture.Executed);
    }

    [Fact]
    public void TheSequenceEndsAtTheControl()
    {
        // THE test the real data forced. ESC FF is the Tektronix erase-screen command - the C0 is
        // the end of it - so what follows must be read fresh. Staying in Escape ate the GS and the
        // coordinate bytes after it and lost the first vector of every plot.
        var capture = Feed("\u001b\u000c");

        Assert.Contains((byte)0x0C, capture.Executed);
        Assert.Equal(ParserState.Ground, capture.FinalState);
    }

    [Fact]
    public void WhatFollowsAnEscFfIsReadFresh()
    {
        // The same thing stated the way a plot sees it: after ESC FF, a GS must arrive as a GS.
        var capture = Feed("\u001b\u000c\u001d");

        Assert.Equal(new[] { (byte)0x0C, (byte)0x1D }, capture.Executed);
        Assert.Equal(ParserState.Ground, capture.FinalState);
    }

    [Fact]
    public void ACsiAfterAnEscFfIsNotSwallowed()
    {
        var capture = Feed("\u001b\u000c\u001b[2J");

        Assert.Equal(new[] { 'J' }, capture.CsiFinals);
        Assert.Equal("", capture.Printed.ToString());
    }

    [Fact]
    public void AControlInsideACsiDoesNotEndIt()
    {
        // The CSI path is genuinely different, and stays different: a C0 there executes and the
        // sequence carries on. Parameters are still being collected, and there is no terminal whose
        // CSI is ended by a control code.
        var capture = Feed("\u001b[1\r;5H");

        Assert.Contains((byte)0x0D, capture.Executed);
        Assert.Equal(new[] { 'H' }, capture.CsiFinals);
    }

    [Fact]
    public void AnEscapeStillEndsAtItsFinalByte()
    {
        var capture = Feed("\u001bMtext");

        Assert.Equal("text", capture.Printed.ToString());
    }

    [Fact]
    public void AStrayEscDoesNotEatTheFollowingText()
    {
        // A truncated escape followed by a control and then text. Ending at the control is what
        // keeps the text readable; the version that stayed in Escape took the 'h' of "hello" as its
        // final byte and printed "ello".
        var capture = Feed("\u001b\rhello");

        Assert.Equal("hello", capture.Printed.ToString());
    }
}
