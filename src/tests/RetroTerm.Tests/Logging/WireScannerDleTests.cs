using System;
using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Logging;
using Xunit;

namespace RetroTerm.Tests.Logging;

/// <summary>
/// The Protocol Monitor must decode TDV DLE cursor addressing as a single 3-byte item.
/// Previously it reported 0x10 as an unknown control and then rendered the two coordinate
/// bytes as bogus TEXT, which is what obscured the underlying emulator bug.
/// </summary>
public class WireScannerDleTests
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

    [Theory]
    [InlineData(0x80, 0x80, "row=1 col=1")]
    [InlineData(0x83, 0x82, "row=4 col=3")]
    [InlineData(0x84, 0x82, "row=5 col=3")]
    [InlineData(0x85, 0x82, "row=6 col=3")]
    public void Feed_Dle_DecodesAsOneCursorAddress(int rowByte, int colByte, string expectedArguments)
    {
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x10, rowByte, colByte)));

        Assert.Single(_emitted);
        Assert.Equal(TraceKind.Sequence, _emitted[0].Kind);
        Assert.Equal("DLE", _emitted[0].Mnemonic);
        Assert.Equal(expectedArguments, _emitted[0].Arguments);
        Assert.True(_emitted[0].IsKnown);
        Assert.Equal(3, _emitted[0].Bytes.Length);
    }

    [Fact]
    public void Feed_Dle_CoordinateBytesAreNotEmittedAsText()
    {
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x10, 0x83, 0x82)));

        for (int i = 0; i < _emitted.Count; i++)
        {
            Assert.NotEqual(TraceKind.Text, _emitted[i].Kind);
        }
    }

    [Fact]
    public void Feed_DleSplitAcrossFeeds_IsStillOneItem()
    {
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x10, 0x83)));
        Assert.Empty(_emitted);

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x82)));

        Assert.Single(_emitted);
        Assert.Equal("row=4 col=3", _emitted[0].Arguments);
    }

    [Fact]
    public void Feed_DleThenText_SeparatesTheTwo()
    {
        var scanner = CreateScanner();

        var data = new List<byte>();
        data.AddRange(Bytes(0x10, 0x83, 0x82));
        data.AddRange(Encoding.ASCII.GetBytes("LINKER:INIT"));

        scanner.Feed(new ReadOnlySpan<byte>(data.ToArray()));

        Assert.Equal(2, _emitted.Count);
        Assert.Equal("DLE", _emitted[0].Mnemonic);
        Assert.Equal(TraceKind.Text, _emitted[1].Kind);
        Assert.Equal("\"LINKER:INIT\"", _emitted[1].Rendered);
    }

    [Fact]
    public void Feed_Dle_RenderedFormShowsTheRawCoordinateBytes()
    {
        var scanner = CreateScanner();

        scanner.Feed(new ReadOnlySpan<byte>(Bytes(0x10, 0x83, 0x82)));

        Assert.Equal("DLE 83 82", _emitted[0].Rendered);
    }
}
