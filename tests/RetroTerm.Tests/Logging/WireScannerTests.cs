using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Logging;
using Xunit;

namespace RetroTerm.Tests.Logging;

/// <summary>
/// Verifies the protocol-trace wire scanner: it must split a raw stream into individual
/// sequences, controls and text runs, and must survive sequences split across network reads.
/// </summary>
public class WireScannerTests
{
    private readonly List<ProtocolTraceEntry> _emitted = new();

    private WireScanner CreateScanner()
    {
        return new WireScanner(TraceDirection.Rx, entry => _emitted.Add(entry));
    }

    private static byte[] Bytes(params int[] values)
    {
        var result = new byte[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = (byte)values[i];
        return result;
    }

    [Fact]
    public void Feed_SingleCsiSequence_EmitsOneDecodedEntry()
    {
        var scanner = CreateScanner();

        // ESC [ 0 m
        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x1B, 0x5B, 0x30, 0x6D)));

        Assert.Single(_emitted);
        Assert.Equal(TraceKind.Sequence, _emitted[0].Kind);
        Assert.Equal("SGR", _emitted[0].Mnemonic);
        Assert.Equal("ESC[0m", _emitted[0].Rendered);
        Assert.Equal(4, _emitted[0].Bytes.Length);
    }

    [Fact]
    public void Feed_CsiWithTwoParameters_DecodesBoth()
    {
        var scanner = CreateScanner();

        // ESC [ 7 ; 3 H
        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x1B, 0x5B, 0x37, 0x3B, 0x33, 0x48)));

        Assert.Single(_emitted);
        Assert.Equal("CUP", _emitted[0].Mnemonic);
        Assert.Equal("row=7 col=3", _emitted[0].Arguments);
    }

    [Fact]
    public void Feed_SequenceSplitAcrossTwoFeeds_IsStillDecodedAsOneItem()
    {
        // This is the exact split seen in the real log: a packet ended with "1B 5B" and the
        // next one began with "36 3B 33 48". The old log showed two lines of garbage.
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x1B, 0x5B)));
        Assert.Empty(_emitted);

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x36, 0x3B, 0x33, 0x48)));

        Assert.Single(_emitted);
        Assert.Equal("CUP", _emitted[0].Mnemonic);
        Assert.Equal("row=6 col=3", _emitted[0].Arguments);
        Assert.Equal(6, _emitted[0].Bytes.Length);
    }

    [Fact]
    public void Feed_PrintableRun_IsEmittedAsOneTextEntry()
    {
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(Encoding.ASCII.GetBytes("LIST-STATUS")));

        Assert.Single(_emitted);
        Assert.Equal(TraceKind.Text, _emitted[0].Kind);
        Assert.Equal("\"LIST-STATUS\"", _emitted[0].Rendered);
        Assert.Equal("11 chars", _emitted[0].Name);
    }

    [Fact]
    public void Feed_TextThenSequence_FlushesTextBeforeTheSequence()
    {
        var scanner = CreateScanner();

        var data = new List<byte>();
        data.AddRange(Encoding.ASCII.GetBytes("AB"));
        data.AddRange(Bytes(0x1B, 0x5B, 0x30, 0x6D));

        scanner.Feed(new ReadOnlySpan<byte>(data.ToArray()));

        Assert.Equal(2, _emitted.Count);
        Assert.Equal(TraceKind.Text, _emitted[0].Kind);
        Assert.Equal(TraceKind.Sequence, _emitted[1].Kind);
    }

    [Fact]
    public void Feed_ControlCharacter_IsEmittedSeparately()
    {
        var scanner = CreateScanner();

        // "OK" CR LF
        var data = new List<byte>();
        data.AddRange(Encoding.ASCII.GetBytes("OK"));
        data.AddRange(Bytes(0x0D, 0x0A));

        scanner.Feed(new ReadOnlySpan<byte>(data.ToArray()));

        Assert.Equal(3, _emitted.Count);
        Assert.Equal(TraceKind.Text, _emitted[0].Kind);
        Assert.Equal("CR", _emitted[1].Mnemonic);
        Assert.Equal("LF", _emitted[2].Mnemonic);
    }

    [Fact]
    public void Feed_TheRealLogSample_ProducesTheExpectedSequenceOfItems()
    {
        // The 57-byte payload from the sample log:
        //   "6;3H" ESC[0K ESC[6;3H "LIST-STATUS" CR LF ESC[2m "Domain or segment name" ESC[0m
        // Note the leading "6;3H" is the tail of a CUP split across packets, so on its own it
        // is plain text - which is exactly what the scanner should report.
        var scanner = CreateScanner();

        var data = new List<byte>();
        data.AddRange(Encoding.ASCII.GetBytes("6;3H"));
        data.AddRange(Bytes(0x1B, 0x5B, 0x30, 0x4B));
        data.AddRange(Bytes(0x1B, 0x5B, 0x36, 0x3B, 0x33, 0x48));
        data.AddRange(Encoding.ASCII.GetBytes("LIST-STATUS"));
        data.AddRange(Bytes(0x0D, 0x0A));
        data.AddRange(Bytes(0x1B, 0x5B, 0x32, 0x6D));
        data.AddRange(Encoding.ASCII.GetBytes("Domain or segment name"));
        data.AddRange(Bytes(0x1B, 0x5B, 0x30, 0x6D));

        scanner.Feed(new ReadOnlySpan<byte>(data.ToArray()));

        Assert.Equal(9, _emitted.Count);

        Assert.Equal(TraceKind.Text, _emitted[0].Kind);          // "6;3H"
        Assert.Equal("EL", _emitted[1].Mnemonic);                // ESC[0K
        Assert.Equal("CUP", _emitted[2].Mnemonic);               // ESC[6;3H
        Assert.Equal("row=6 col=3", _emitted[2].Arguments);
        Assert.Equal(TraceKind.Text, _emitted[3].Kind);          // "LIST-STATUS"
        Assert.Equal("CR", _emitted[4].Mnemonic);
        Assert.Equal("LF", _emitted[5].Mnemonic);
        Assert.Equal("SGR", _emitted[6].Mnemonic);               // ESC[2m
        Assert.Equal("Dim", _emitted[6].Arguments);
        Assert.Equal(TraceKind.Text, _emitted[7].Kind);          // "Domain or segment name"
        Assert.Equal("SGR", _emitted[8].Mnemonic);               // ESC[0m
        Assert.Equal("Reset attributes", _emitted[8].Arguments);
    }

    [Fact]
    public void Feed_DecPrivateSequence_KeepsThePrivateMarker()
    {
        var scanner = CreateScanner();

        // ESC [ ? 2 5 h
        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x1B, 0x5B, 0x3F, 0x32, 0x35, 0x68)));

        Assert.Single(_emitted);
        Assert.Equal("DECSET", _emitted[0].Mnemonic);
        Assert.Equal("ESC[?25h", _emitted[0].Rendered);
    }

    [Fact]
    public void Feed_UnknownSequence_IsMarkedNotKnown()
    {
        var scanner = CreateScanner();

        // ESC [ Q - a valid final byte with no assigned function.
        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x1B, 0x5B, 0x51)));

        Assert.Single(_emitted);
        Assert.False(_emitted[0].IsKnown);
    }

    [Fact]
    public void Feed_OscString_IsCapturedAsOneStringCommand()
    {
        var scanner = CreateScanner();

        // ESC ] 0 ; T i t l e BEL
        var data = new List<byte>();
        data.AddRange(Bytes(0x1B, 0x5D));
        data.AddRange(Encoding.ASCII.GetBytes("0;Title"));
        data.Add(0x07);

        scanner.Feed(new ReadOnlySpan<byte>(data.ToArray()));

        Assert.Single(_emitted);
        Assert.Equal(TraceKind.StringCommand, _emitted[0].Kind);
        Assert.Equal("OSC", _emitted[0].Mnemonic);
    }

    [Fact]
    public void Reset_DiscardsAPartialSequence()
    {
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x1B, 0x5B, 0x37)));
        scanner.Reset();
        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x1B, 0x5B, 0x30, 0x6D)));

        Assert.Single(_emitted);
        Assert.Equal("SGR", _emitted[0].Mnemonic);
    }

    [Fact]
    public void Feed_EscapeSequenceWithIntermediate_IsDecoded()
    {
        var scanner = CreateScanner();

        // ESC ( B - designate US ASCII into G0
        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x1B, 0x28, 0x42)));

        Assert.Single(_emitted);
        Assert.Equal("SCS", _emitted[0].Mnemonic);
        Assert.Equal("G0 = US ASCII", _emitted[0].Arguments);
    }

    [Fact]
    public void Direction_IsCarriedOntoEveryEntry()
    {
        var entries = new List<ProtocolTraceEntry>();
        var scanner = new WireScanner(TraceDirection.Tx, e => entries.Add(e));

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x0D)));

        Assert.Single(entries);
        Assert.Equal(TraceDirection.Tx, entries[0].Direction);
        Assert.Equal("TX", entries[0].DirectionTag);
    }
}
