using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Parsing;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Regression tests for the four defects fixed in the Phase 0 architecture cleanup
/// (see docs\ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md, appendix items 1-4).
///
/// Every one of these bugs survived a 3000-test suite because nothing covered the path.
/// These tests exist so they cannot come back silently.
/// </summary>
public class Phase0RegressionTests
{
    // ─────────────────────────────────────────────────────────────
    // Bug 1 — 8-bit C1 controls were unreachable dead code.
    // The ">= 0x80 means UTF-8 lead byte" branch was tested BEFORE the
    // C1 checks, so 0x9B/0x90/0x9D became U+FFFD instead of CSI/DCS/OSC.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void C1_Csi_0x9B_StartsCsiSequence_NotReplacementChar()
    {
        var parser = new EscapeSequenceParser();
        char? csiFinal = null;
        var characters = new List<uint>();
        parser.OnCsiDispatch += p => csiFinal = (char)p.FinalByte;
        parser.OnCharacter += cp => characters.Add(cp);

        // 8-bit CSI (0x9B) + "1;1H" — the 8-bit form of ESC [ 1 ; 1 H
        parser.ProcessBytes(new byte[] { 0x9B, (byte)'1', (byte)';', (byte)'1', (byte)'H' });

        Assert.Equal('H', csiFinal);
        Assert.DoesNotContain(0xFFFDu, characters);
    }

    [Fact]
    public void C1_Csi_0x9B_CarriesParameters()
    {
        var parser = new EscapeSequenceParser();
        int p0 = -1, p1 = -1;
        parser.OnCsiDispatch += p =>
        {
            p0 = p.GetParam(0, -1);
            p1 = p.GetParam(1, -1);
        };

        parser.ProcessBytes(new byte[] { 0x9B, (byte)'5', (byte)';', (byte)'9', (byte)'H' });

        Assert.Equal(5, p0);
        Assert.Equal(9, p1);
    }

    [Fact]
    public void C1_Osc_0x9D_StartsOscString()
    {
        var parser = new EscapeSequenceParser();
        string? osc = null;
        parser.OnOscDispatch += data => osc = Encoding.ASCII.GetString(data.ToArray());

        // 0x9D "0;Hi" BEL — the 8-bit form of ESC ] 0 ; Hi BEL (set title)
        var bytes = new List<byte> { 0x9D };
        bytes.AddRange(Encoding.ASCII.GetBytes("0;Hi"));
        bytes.Add(0x07);
        parser.ProcessBytes(bytes.ToArray());

        Assert.Equal("0;Hi", osc);
    }

    [Fact]
    public void C1_Dcs_0x90_ReachesDcsHook()
    {
        var parser = new EscapeSequenceParser();
        char? dcsFinal = null;
        parser.OnDcsHook += p => dcsFinal = (char)p.FinalByte;

        // 0x90 'q' — the 8-bit form of ESC P q (a Sixel introducer without params)
        parser.ProcessBytes(new byte[] { 0x90, (byte)'q' });

        Assert.Equal('q', dcsFinal);
    }

    [Theory]
    [InlineData((byte)0x84)] // IND  - Index
    [InlineData((byte)0x85)] // NEL  - Next Line
    [InlineData((byte)0x88)] // HTS  - Horizontal Tab Set
    [InlineData((byte)0x8D)] // RI   - Reverse Index
    public void C1_SingleByteControls_GoToExecute_NotReplacementChar(byte c1)
    {
        var parser = new EscapeSequenceParser();
        var executed = new List<byte>();
        var characters = new List<uint>();
        parser.OnExecute += b => executed.Add(b);
        parser.OnCharacter += cp => characters.Add(cp);

        parser.ProcessBytes(new byte[] { c1 });

        Assert.Contains(c1, executed);
        Assert.DoesNotContain(0xFFFDu, characters);
    }

    [Fact]
    public void Utf8_StillDecodes_AfterC1Reordering()
    {
        // Guard against the C1 fix stealing genuine UTF-8 lead bytes.
        // 0xC3 0xA6 is 'æ' (U+00E6); 0xE2 0x86 0x92 is '→' (U+2192).
        var parser = new EscapeSequenceParser();
        var characters = new List<uint>();
        parser.OnCharacter += cp => characters.Add(cp);

        parser.ProcessBytes(new byte[] { 0xC3, 0xA6, 0xE2, 0x86, 0x92 });

        Assert.Equal(new List<uint> { 0x00E6, 0x2192 }, characters);
    }

    // ─────────────────────────────────────────────────────────────
    // Bug 2 — the VT reply channel was never wired, and Primary DA
    // was not implemented at all, so the terminal stayed mute.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PrimaryDeviceAttributes_AreAnswered()
    {
        var emulator = new VT100Emulator(80, 24);
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[c"));

        Assert.Single(replies);
        Assert.Equal("\x1b[?1;2c", replies[0]);
    }

    [Fact]
    public void PrimaryDeviceAttributes_ExplicitZeroParameter_IsAnswered()
    {
        var emulator = new VT100Emulator(80, 24);
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[0c"));

        Assert.Single(replies);
        Assert.Equal("\x1b[?1;2c", replies[0]);
    }

    [Fact]
    public void SecondaryDeviceAttributes_AreAnswered()
    {
        var emulator = new VT100Emulator(80, 24);
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[>c"));

        Assert.Single(replies);
        Assert.Equal("\x1b[>0;10;0c", replies[0]);
    }

    [Fact]
    public void DeviceAttributes_NonZeroParameter_IsIgnored()
    {
        // DEC: a DA request carries no parameter or an explicit 0. CSI 1 c is not a
        // DA request and must not be answered.
        var emulator = new VT100Emulator(80, 24);
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[1c"));

        Assert.Empty(replies);
    }

    [Fact]
    public void CursorPositionReport_IsAnswered()
    {
        var emulator = new VT100Emulator(80, 24);
        var replies = new List<string>();
        emulator.DataToSend += bytes => replies.Add(Encoding.ASCII.GetString(bytes));

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[5;9H")); // row 5, col 9
        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[6n"));   // DSR - report cursor

        Assert.Single(replies);
        Assert.Equal("\x1b[5;9R", replies[0]);
    }

    [Fact]
    public async Task Session_SendsEmulatorReplyToConnection()
    {
        // The whole point of bug 2: the reply must actually reach the wire, not just
        // be raised as an event. Before the fix nothing subscribed to DataToSend.
        var emulator = new VT100Emulator(80, 24);
        using var session = new TerminalSession(emulator, "DaTest");
        var connection = new InMemoryConnection();
        await session.ConnectAsync(connection);

        connection.SimulateReceive(Encoding.ASCII.GetBytes("\x1b[c"));
        await session.FlushAsync();

        var sent = connection.GetSentData();
        Assert.Single(sent);
        Assert.Equal("\x1b[?1;2c", Encoding.ASCII.GetString(sent[0]));
    }

    // ─────────────────────────────────────────────────────────────
    // Bug 4 — TDV2215 used an impossible wrap condition
    // ("Cursor.Row > ScrollBottom"), so it never scrolled at the
    // bottom of a scrolling region while TDV2200 did.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tdv2215_ScrollsWhenWrappingAtScrollRegionBottom()
    {
        var emulator = new TDV2215Emulator(20, 10);

        // Scroll region rows 1-3 (1-based) = rows 0-2 (0-based)
        emulator.ProcessInput(Encoding.ASCII.GetBytes("\x1b[1;3r"));
        // Put a marker on the region's top row, then fill the bottom row so the
        // next character must wrap and force a scroll.
        emulator.ProcessInput(Encoding.ASCII.GetBytes("\x1b[1;1H"));
        emulator.ProcessInput(Encoding.ASCII.GetBytes("TOPROW"));
        emulator.ProcessInput(Encoding.ASCII.GetBytes("\x1b[3;1H"));
        // 19 characters on a 20-column screen: this fills columns 1-19 and leaves the
        // cursor on the last column WITHOUT wrapping. A 20th character here would
        // already wrap and scroll, which is what the marker check below would miss.
        emulator.ProcessInput(Encoding.ASCII.GetBytes(new string('X', 19)));

        var bufferBefore = emulator.GetBuffer();
        Assert.Equal('T', (char)bufferBefore[0, 0].Codepoint);

        // Two more characters: the first lands on the last column, the second forces the
        // wrap past the region bottom → the region must scroll up, pushing "TOPROW" off.
        emulator.ProcessInput(Encoding.ASCII.GetBytes("YZ"));

        var buffer = emulator.GetBuffer();
        Assert.NotEqual('T', (char)buffer[0, 0].Codepoint);
    }

    [Fact]
    public void Tdv2215_DoesNotScrollWhenWritingOutsideScrollRegion()
    {
        // The guard on the other side: writing below the region must not scroll it.
        var emulator = new TDV2215Emulator(20, 10);

        emulator.ProcessInput(Encoding.ASCII.GetBytes("\x1b[1;3r")); // region rows 0-2
        emulator.ProcessInput(Encoding.ASCII.GetBytes("\x1b[1;1H"));
        emulator.ProcessInput(Encoding.ASCII.GetBytes("TOPROW"));

        // Fill row 5 (outside the region) so it wraps there
        emulator.ProcessInput(Encoding.ASCII.GetBytes("\x1b[5;1H"));
        emulator.ProcessInput(Encoding.ASCII.GetBytes(new string('X', 21)));

        var buffer = emulator.GetBuffer();
        Assert.Equal('T', (char)buffer[0, 0].Codepoint);
    }
}
