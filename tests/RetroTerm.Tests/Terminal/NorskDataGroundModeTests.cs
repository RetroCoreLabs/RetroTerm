using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Parsing;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The <c>ESC "</c> ground mode — the seam that lets a Norsk Data graphic terminal's own sequences
/// be tokenised without changing how anything else reads the same bytes.
///
/// THE SITUATION, WHICH IS NOT A PARSER BUG. <c>ESC "5d</c> arrives and the parser reads it as an
/// escape with intermediate <c>"</c> and final <c>5</c>, leaving the <c>d</c> to be printed on
/// screen. That is CORRECT ECMA-48: 0x22 sits in the 0x20–0x2F intermediate range and 0x35 is a
/// legal final. On a VT, an xterm, or anything else that is not an ND, it is exactly the right
/// reading — so this cannot be "fixed" globally, only switched on by a profile that knows what it
/// is talking to.
///
/// That is why the flag is off by default and why these tests spend as much effort on what happens
/// when it is OFF as on what happens when it is on. The architecture review calls this the ground-
/// mode seam (section C.1); VT52's binary coordinates and Tektronix's vector bytes will need the
/// same shape, and TDV already built its own version outside the parser because the parser had none.
/// </summary>
public class NorskDataGroundModeTests
{
    /// <summary>
    /// What one completed sequence looked like.
    /// </summary>
    private sealed class Dispatch
    {
        public char Final;
        public int[] Parameters = System.Array.Empty<int>();
        public string Intermediates = "";
    }

    private sealed class Capture
    {
        public readonly List<Dispatch> NorskData = new List<Dispatch>();
        public readonly List<Dispatch> Escapes = new List<Dispatch>();
        public readonly List<Dispatch> Csi = new List<Dispatch>();
        public readonly List<byte> Executed = new List<byte>();
        public readonly StringBuilder Printed = new StringBuilder();
    }

    private static Capture Feed(string text, bool norskDataMode)
    {
        var parser = new EscapeSequenceParser();
        parser.NorskDataGraphicsSequences = norskDataMode;

        var capture = new Capture();

        parser.OnCharacter += cp => capture.Printed.Append((char)cp);
        parser.OnNorskDataDispatch += p => capture.NorskData.Add(Snapshot(p));
        parser.OnEscapeDispatch += p => capture.Escapes.Add(Snapshot(p));
        parser.OnCsiDispatch += p => capture.Csi.Add(Snapshot(p));
        parser.OnExecute += b => capture.Executed.Add(b);

        parser.ProcessBytes(Encoding.ASCII.GetBytes(text));
        return capture;
    }

    private static Dispatch Snapshot(EscapeSequenceParser parser)
    {
        var source = parser.Parameters;
        var parameters = new int[source.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            parameters[i] = source[i];
        }

        var intermediates = new StringBuilder();
        var bytes = parser.Intermediates;
        for (int i = 0; i < bytes.Length; i++)
        {
            intermediates.Append((char)bytes[i]);
        }

        return new Dispatch
        {
            Final = (char)parser.FinalByte,
            Parameters = parameters,
            Intermediates = intermediates.ToString(),
        };
    }

    // ─────────────────────────────────────────────────────────────
    // With the mode OFF — the default, and it must not move
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ByDefaultTheSequenceIsReadAsOrdinaryEcma48()
    {
        // Not a bug being tolerated: this is the correct reading for a terminal that is not an ND,
        // and the whole reason the mode has to be opt-in.
        var capture = Feed("\u001b\"5d", norskDataMode: false);

        Assert.Empty(capture.NorskData);
        Assert.Single(capture.Escapes);
        Assert.Equal('5', capture.Escapes[0].Final);
        Assert.Equal("\"", capture.Escapes[0].Intermediates);
        Assert.Equal("d", capture.Printed.ToString());
    }

    [Fact]
    public void TheModeIsOffOnAFreshParser()
    {
        var parser = new EscapeSequenceParser();

        Assert.False(parser.NorskDataGraphicsSequences);
    }

    // ─────────────────────────────────────────────────────────────
    // With the mode ON
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheSequenceIsTokenisedAsOneCommand()
    {
        // THE test. Same bytes, and now nothing is printed and one ND command comes out.
        var capture = Feed("\u001b\"5d", norskDataMode: true);

        Assert.Single(capture.NorskData);
        Assert.Equal('d', capture.NorskData[0].Final);
        Assert.Equal(new[] { 5 }, capture.NorskData[0].Parameters);
        Assert.Empty(capture.Escapes);
        Assert.Equal("", capture.Printed.ToString());
    }

    [Fact]
    public void SeveralParametersAreSeparatedBySemicolons()
    {
        // Straight from the spec: ESC "13;10l disables inking mode.
        var capture = Feed("\u001b\"13;10l", norskDataMode: true);

        Assert.Single(capture.NorskData);
        Assert.Equal('l', capture.NorskData[0].Final);
        Assert.Equal(new[] { 13, 10 }, capture.NorskData[0].Parameters);
    }

    [Fact]
    public void TheDetectionSequenceFromTheSpecTokenisesWhole()
    {
        // The real detection burst an ND test program sends: US, then two ND commands, then the
        // standard Tek identification request. Nothing here may leak onto the screen.
        var capture = Feed("\u001b\"13;10l\u001b\"5d\u001b\u0005", norskDataMode: true);

        Assert.Equal(2, capture.NorskData.Count);
        Assert.Equal('l', capture.NorskData[0].Final);
        Assert.Equal(new[] { 13, 10 }, capture.NorskData[0].Parameters);
        Assert.Equal('d', capture.NorskData[1].Final);
        Assert.Equal(new[] { 5 }, capture.NorskData[1].Parameters);

        Assert.Equal("", capture.Printed.ToString());

        // The burst ends with ESC ENQ, the standard Tektronix identification request. It arrives as
        // an executed ENQ rather than an escape dispatch - ENQ is a C0, not a final byte - which is
        // correct. What makes it USABLE is that the parser stays in the Escape state while running
        // it, so a handler can tell ESC ENQ from a lone ENQ; see
        // AnEnqInsideAnEscapeIsDistinguishableFromABareOne.
        Assert.Contains((byte)0x05, capture.Executed);
        Assert.Empty(capture.Escapes);
    }

    [Fact]
    public void ASequenceWithNoParametersStillDispatches()
    {
        var capture = Feed("\u001b\"h", norskDataMode: true);

        Assert.Single(capture.NorskData);
        Assert.Equal('h', capture.NorskData[0].Final);
    }

    [Fact]
    public void OrdinaryTextAroundTheSequenceIsUntouched()
    {
        var capture = Feed("before\u001b\"5dafter", norskDataMode: true);

        Assert.Single(capture.NorskData);
        Assert.Equal("beforeafter", capture.Printed.ToString());
    }

    // ─────────────────────────────────────────────────────────────
    // The mode must not eat anything else
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void OrdinaryCsiStillWorksWithTheModeOn()
    {
        // The guard against overreach. An ND terminal is a TEXT terminal that also does graphics,
        // so every ordinary sequence has to keep working while the mode is on.
        var capture = Feed("\u001b[2J\u001b[1;5H", norskDataMode: true);

        Assert.Equal(2, capture.Csi.Count);
        Assert.Equal('J', capture.Csi[0].Final);
        Assert.Equal('H', capture.Csi[1].Final);
        Assert.Empty(capture.NorskData);
    }

    [Fact]
    public void OtherEscapeIntermediatesAreUnaffected()
    {
        // ESC # 6 is DECDWL and its intermediate is 0x23, right next to 0x22. Only the quote is
        // special, and only when the mode is on.
        var capture = Feed("\u001b#6", norskDataMode: true);

        Assert.Single(capture.Escapes);
        Assert.Equal('6', capture.Escapes[0].Final);
        Assert.Equal("#", capture.Escapes[0].Intermediates);
        Assert.Empty(capture.NorskData);
    }

    [Fact]
    public void DecscaIsNotMistakenForAnNdSequence()
    {
        // DECSCA is CSI Ps " q - a quote INSIDE a CSI, as an intermediate. The ND mode watches for
        // a quote straight after ESC, so these must not collide. If they did, every protected
        // field on a 2200 would break the moment graphics were enabled.
        var capture = Feed("\u001b[1\"q", norskDataMode: true);

        Assert.Single(capture.Csi);
        Assert.Equal('q', capture.Csi[0].Final);
        Assert.Equal("\"", capture.Csi[0].Intermediates);
        Assert.Empty(capture.NorskData);
    }

    // ─────────────────────────────────────────────────────────────
    // Malformed input
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnInterruptedSequenceDoesNotSwallowTheNextOne()
    {
        // A truncated sequence followed by a real one. Without the ESC restart the second would be
        // eaten as parameters of the first and both would be lost.
        var capture = Feed("\u001b\"13\u001b[2J", norskDataMode: true);

        Assert.Empty(capture.NorskData);
        Assert.Single(capture.Csi);
        Assert.Equal('J', capture.Csi[0].Final);
    }

    [Fact]
    public void RubbishInsideASequenceEndsItRatherThanCollecting()
    {
        var capture = Feed("\u001b\"1?2d", norskDataMode: true);

        // '?' is not part of the grammar; the sequence is abandoned and what follows is text.
        Assert.Empty(capture.NorskData);
        Assert.Equal("2d", capture.Printed.ToString());
    }

    [Fact]
    public void AControlCodeInsideASequenceStillExecutes()
    {
        // A host is entitled to send CR mid-sequence, and dropping it would lose a line break.
        var executed = new List<byte>();
        var parser = new EscapeSequenceParser { NorskDataGraphicsSequences = true };
        parser.OnExecute += b => executed.Add(b);

        var dispatched = 0;
        parser.OnNorskDataDispatch += _ => dispatched++;

        parser.ProcessBytes(Encoding.ASCII.GetBytes("\u001b\"1\r3d"));

        Assert.Contains((byte)0x0D, executed);
        Assert.Equal(1, dispatched);
    }
}
