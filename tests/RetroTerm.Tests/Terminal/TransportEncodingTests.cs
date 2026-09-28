using System;
using System.Text;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Profiles;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Phase 4 part 4: the terminal decides how typed text becomes bytes.
///
/// TerminalSession.SendInputAsync called Encoding.UTF8.GetBytes for EVERY session, whatever
/// terminal was attached. That is right for a modern host behind an ANSI/VT session and wrong for
/// an ND host behind a TDV one: a TDV line is 8-bit, so one typed character has to leave as one
/// byte. Under UTF-8 anything above 0x7F went out as TWO bytes and the host read two garbage
/// characters.
///
/// The ISO 646 wire conversion (appendix 0o) hid this for the characters people actually type on a
/// Norwegian keyboard — Æ Ø Å are replaced with ASCII positions before they reach the send path —
/// so the visible symptom only showed up for everything else. That is why it survived this long.
///
/// The encoding now hangs off TerminalProfile, next to the rest of the terminal's identity.
/// </summary>
public class TransportEncodingTests
{
    // ─────────────────────────────────────────────────────────────
    // Which terminal gets which encoding
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TdvTerminalsSendEightBit()
    {
        Assert.Equal(TransportEncoding.EightBit, new TDV2200Emulator(80, 24).Profile.TransportEncoding);
        Assert.Equal(TransportEncoding.EightBit, new TDV2215Emulator(80, 24).Profile.TransportEncoding);
        Assert.Equal(TransportEncoding.EightBit, new TDV1200Emulator(80, 24).Profile.TransportEncoding);
    }

    [Fact]
    public void TheVtFamilySendsUtf8()
    {
        // Deliberate, and a product decision rather than a hardware fact: a real VT100 was 7-bit
        // ASCII. These sessions reach modern hosts, where typing é into an ssh session has to work,
        // so they keep the UTF-8 behaviour they already had. A strict-hardware profile could say
        // otherwise; that is someone's decision to make, not something to change silently here.
        Assert.Equal(TransportEncoding.Utf8, new VT100Emulator(80, 24).Profile.TransportEncoding);
        Assert.Equal(TransportEncoding.Utf8, TerminalProfile.Ansi.TransportEncoding);
    }

    // ─────────────────────────────────────────────────────────────
    // The encoding itself
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AsciiIsTheSameEitherWay()
    {
        var expected = Encoding.ASCII.GetBytes("HELLO");

        Assert.Equal(expected, TerminalProfile.VT100.EncodeForTransport("HELLO"));
        Assert.Equal(expected, new TDV2200Emulator(80, 24).Profile.EncodeForTransport("HELLO"));
    }

    [Fact]
    public void AHighCharacterIsOneByteOnAnEightBitLineAndTwoUnderUtf8()
    {
        // THE defect, stated as a length. 'ø' is U+00F8.
        var eightBit = new TDV2200Emulator(80, 24).Profile.EncodeForTransport("ø");
        var utf8 = TerminalProfile.VT100.EncodeForTransport("ø");

        Assert.Single(eightBit);
        Assert.Equal(0xF8, eightBit[0]);
        Assert.Equal(2, utf8.Length);
    }

    [Fact]
    public void EveryCharacterOfAWordKeepsItsOwnByte()
    {
        var bytes = new TDV2200Emulator(80, 24).Profile.EncodeForTransport("BLÅBÆR");

        // One byte per typed character — the property UTF-8 breaks.
        Assert.Equal(6, bytes.Length);
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal(0xC5, bytes[2]);   // Å
        Assert.Equal(0xC6, bytes[4]);   // Æ
    }

    [Fact]
    public void AboveTheByteRangeBecomesAVisibleQuestionMark()
    {
        // No 8-bit line can carry U+4E2D. Substituting '?' is what Encoding.ASCII does; a visible
        // wrong character beats one that silently vanishes and leaves the host's parser mid-field.
        var bytes = new TDV2200Emulator(80, 24).Profile.EncodeForTransport("A中B");

        Assert.Equal(3, bytes.Length);
        Assert.Equal((byte)'?', bytes[1]);
    }

    [Fact]
    public void ASurrogatePairBecomesTwoQuestionMarks()
    {
        // Recorded rather than argued: an emoji is two chars in .NET, and neither half fits in a
        // byte, so it costs two '?'. Nothing on an 8-bit line could carry it under any rule.
        var bytes = new TDV2200Emulator(80, 24).Profile.EncodeForTransport("\U0001F600");

        Assert.Equal(2, bytes.Length);
        Assert.Equal((byte)'?', bytes[0]);
        Assert.Equal((byte)'?', bytes[1]);
    }

    [Fact]
    public void EmptyTextEncodesToNothingAndNullThrows()
    {
        Assert.Empty(TerminalProfile.VT100.EncodeForTransport(""));
        Assert.Throws<ArgumentNullException>(() => TerminalProfile.VT100.EncodeForTransport(null!));
    }

    [Fact]
    public void ControlBytesSurviveUnchangedOnBothPaths()
    {
        // ESC and CR are what scripts and the MCP surface send most; they must not be touched.
        var text = new string(new[] { (char)0x1B, (char)0x0D, (char)0x07 });

        Assert.Equal(new byte[] { 0x1B, 0x0D, 0x07 }, new TDV2200Emulator(80, 24).Profile.EncodeForTransport(text));
        Assert.Equal(new byte[] { 0x1B, 0x0D, 0x07 }, TerminalProfile.VT100.EncodeForTransport(text));
    }

    // ─────────────────────────────────────────────────────────────
    // The two encodings agree wherever it is possible to agree
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheTwoEncodingsAgreeOnAllOfAscii()
    {
        // The whole 7-bit range must be byte-identical, or switching a session's terminal type
        // would change what ordinary typing puts on the wire.
        var tdv = new TDV2200Emulator(80, 24).Profile;

        for (int c = 0; c < 0x80; c++)
        {
            string s = ((char)c).ToString();
            Assert.Equal(TerminalProfile.VT100.EncodeForTransport(s), tdv.EncodeForTransport(s));
        }
    }

    [Fact]
    public void TheEightBitPathCoversTheWholeByteRange()
    {
        var profile = new TDV2200Emulator(80, 24).Profile;

        for (int c = 0; c <= 0xFF; c++)
        {
            var bytes = profile.EncodeForTransport(((char)c).ToString());
            Assert.Single(bytes);
            Assert.Equal((byte)c, bytes[0]);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // The receive half: is 0xC5 a lead byte or a character?
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnEightBitTerminalTakesAHighByteAsOneCharacter()
    {
        // The mirror of the send defect, and the worse one: a byte of host output was being eaten.
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(new byte[] { 0xC5, (byte)'A' });

        Assert.Equal(0xC5u, emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal((uint)'A', emulator.Buffer.GetCell(0, 1).Codepoint);
    }

    [Fact]
    public void TwoHighBytesStayTwoCharactersOnAnEightBitLine()
    {
        // 0xC5 0xA9 is a valid UTF-8 pair, so the parser used to fold these two bytes of host
        // output into ONE codepoint — two screen positions collapsing into one wrong glyph.
        var emulator = new TDV2200Emulator(80, 24);

        emulator.ProcessData(new byte[] { 0xC5, 0xA9 });

        Assert.Equal(0xC5u, emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal(0xA9u, emulator.Buffer.GetCell(0, 1).Codepoint);
    }

    [Fact]
    public void AUtf8TerminalStillDecodesUtf8()
    {
        // The other half must not regress: on a VT session those same two bytes ARE one character
        // (U+0169, ũ).
        var emulator = new VT100Emulator(80, 24);

        emulator.ProcessData(new byte[] { 0xC5, 0xA9 });

        Assert.Equal(0x0169u, emulator.Buffer.GetCell(0, 0).Codepoint);
    }

    [Fact]
    public void EscapeSequencesAreUnaffectedOnAnEightBitLine()
    {
        // Guard: the branch that changed sits next to the C1 and control handling. A plain CSI
        // must still be parsed as a sequence, not printed.
        var emulator = new TDV2200Emulator(80, 24);

        // Built from explicit bytes: a "\x1b[" literal is a variable-length escape in C# and has bitten this work three times.
        emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)';', (byte)'3', (byte)'H', (byte)'X' });

        Assert.Equal((uint)'X', emulator.Buffer.GetCell(4, 2).Codepoint);
    }

    [Fact]
    public void AProfileDefaultsToUtf8WhenNotStated()
    {
        // The constructor default. Stated as a test because it decides what any NEW profile does
        // if its author does not think about the wire.
        var profile = new TerminalProfile(
            "Test",
            Encoding.ASCII.GetBytes("\x1b[?1;2c"),
            Encoding.ASCII.GetBytes("\x1b[>0;10;0c"),
            TerminalFeatures.None,
            new[] { 1 });

        Assert.Equal(TransportEncoding.Utf8, profile.TransportEncoding);
    }
}
