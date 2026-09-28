using System;
using System.Text;
using RetroTerm.Core.Logging;
using Xunit;

namespace RetroTerm.Tests.Logging;

/// <summary>
/// Verifies that the diagnostic decoder names the sequences that appeared in the real session
/// log the redesign was driven by, plus the TDV private finals.
/// </summary>
public class EscapeSequenceDecoderTests
{
    private static DecodedSequence Csi(byte finalByte, params int[] parameters)
    {
        return EscapeSequenceDecoder.DecodeCsi(
            0,
            ReadOnlySpan<byte>.Empty,
            new ReadOnlySpan<int>(parameters),
            finalByte);
    }

    // ---- The two sequences that dominated the original log ----

    [Fact]
    public void DecodeCsi_Sgr0_IsNamedAndDescribed()
    {
        // The log was full of "Final=0x6D, Params=0" with no indication it meant a reset.
        var decoded = Csi((byte)'m', 0);

        Assert.True(decoded.IsKnown);
        Assert.Equal("SGR", decoded.Mnemonic);
        Assert.Equal("Select Graphic Rendition", decoded.Name);
        Assert.Equal("Reset attributes", decoded.Arguments);
        Assert.Equal("ESC[0m", decoded.Rendered);
    }

    [Fact]
    public void DecodeCsi_Cup_ReportsRowAndColumn()
    {
        // "Final=0x48, Params=7,3" must read as row 7, column 3.
        var decoded = Csi((byte)'H', 7, 3);

        Assert.True(decoded.IsKnown);
        Assert.Equal("CUP", decoded.Mnemonic);
        Assert.Equal("row=7 col=3", decoded.Arguments);
        Assert.Equal("ESC[7;3H", decoded.Rendered);
    }

    [Fact]
    public void DecodeCsi_Sgr2_IsDim()
    {
        var decoded = Csi((byte)'m', 2);

        Assert.Equal("Dim", decoded.Arguments);
    }

    [Fact]
    public void DecodeCsi_EraseInLine_DescribesTheMode()
    {
        var decoded = Csi((byte)'K', 0);

        Assert.Equal("EL", decoded.Mnemonic);
        Assert.Equal("cursor to end of line", decoded.Arguments);
    }

    // ---- Parameter defaulting ----

    [Fact]
    public void DecodeCsi_CursorUpWithNoParameters_DefaultsToOne()
    {
        var decoded = Csi((byte)'A');

        Assert.Equal("CUU", decoded.Mnemonic);
        Assert.Equal("n=1", decoded.Arguments);
    }

    [Fact]
    public void DecodeCsi_CupWithNoParameters_DefaultsToHome()
    {
        var decoded = Csi((byte)'H');

        Assert.Equal("row=1 col=1", decoded.Arguments);
    }

    // ---- SGR composition ----

    [Fact]
    public void DescribeSgr_MultipleParameters_AreListedInOrder()
    {
        var parameters = new[] { 1, 31, 4 };
        var text = EscapeSequenceDecoder.DescribeSgr(new ReadOnlySpan<int>(parameters));

        Assert.Equal("Bold, FG Red, Underline", text);
    }

    [Fact]
    public void DescribeSgr_IndexedColor_IsCollapsedToOneItem()
    {
        var parameters = new[] { 38, 5, 202 };
        var text = EscapeSequenceDecoder.DescribeSgr(new ReadOnlySpan<int>(parameters));

        Assert.Equal("FG index 202", text);
    }

    [Fact]
    public void DescribeSgr_TrueColor_IsCollapsedToOneItem()
    {
        var parameters = new[] { 48, 2, 10, 20, 30 };
        var text = EscapeSequenceDecoder.DescribeSgr(new ReadOnlySpan<int>(parameters));

        Assert.Equal("BG rgb(10,20,30)", text);
    }

    [Fact]
    public void DescribeSgr_NoParameters_MeansReset()
    {
        var text = EscapeSequenceDecoder.DescribeSgr(ReadOnlySpan<int>.Empty);

        Assert.Equal("Reset attributes", text);
    }

    // ---- DEC private modes ----

    [Fact]
    public void DecodeCsi_DecPrivateSet_IsNamedDecset()
    {
        var parameters = new[] { 25 };
        var decoded = EscapeSequenceDecoder.DecodeCsi(
            (byte)'?', ReadOnlySpan<byte>.Empty, new ReadOnlySpan<int>(parameters), (byte)'h');

        Assert.True(decoded.IsKnown);
        Assert.Equal("DECSET", decoded.Mnemonic);
        Assert.Contains("DECTCEM cursor visible", decoded.Arguments);
        Assert.Contains("ON", decoded.Arguments);
        Assert.Equal("ESC[?25h", decoded.Rendered);
    }

    [Fact]
    public void DecodeCsi_DecPrivateReset_IsNamedDecrst()
    {
        var parameters = new[] { 1049 };
        var decoded = EscapeSequenceDecoder.DecodeCsi(
            (byte)'?', ReadOnlySpan<byte>.Empty, new ReadOnlySpan<int>(parameters), (byte)'l');

        Assert.Equal("DECRST", decoded.Mnemonic);
        Assert.Contains("OFF", decoded.Arguments);
    }

    // ---- TDV private finals ----

    [Theory]
    [InlineData((byte)'z', "NDSAR")]
    [InlineData((byte)'{', "NDAAR")]
    [InlineData((byte)'|', "NDRAR")]
    [InlineData((byte)'}', "NDFC")]
    [InlineData((byte)'~', "NDDWA")]
    [InlineData((byte)'<', "NDVIDEO")]
    public void DecodeCsi_TdvPrivateFinals_AreNamed(byte finalByte, string expectedMnemonic)
    {
        var decoded = Csi(finalByte, 1, 2, 3, 4);

        Assert.True(decoded.IsKnown);
        Assert.Equal(expectedMnemonic, decoded.Mnemonic);
    }

    [Fact]
    public void DecodeCsi_TdvRectangle_ReportsCorners()
    {
        var decoded = Csi((byte)'z', 2, 5, 10, 40);

        Assert.Equal("top=2 left=5 bottom=10 right=40", decoded.Arguments);
    }

    [Fact]
    public void DecodeCsi_TdvFunctionKeyReport_IsNamed()
    {
        // F1 (HJELP) reports as ESC[46_ per the TDV2200 key registry.
        var decoded = Csi((byte)'_', 46);

        Assert.True(decoded.IsKnown);
        Assert.Equal("TDVKEY", decoded.Mnemonic);
        Assert.Equal("key=46", decoded.Arguments);
        Assert.Equal("ESC[46_", decoded.Rendered);
    }

    // ---- Unknown sequences must be flagged, not silently accepted ----

    [Fact]
    public void DecodeCsi_UnrecognisedFinal_IsFlaggedUnknown()
    {
        // 'Q' is a valid CSI final byte but has no assigned function in ECMA-48 or on the TDV.
        var decoded = Csi((byte)'Q');

        Assert.False(decoded.IsKnown);
        Assert.Equal("UNKNOWN", decoded.Mnemonic);
    }

    // ---- Plain escape sequences ----

    [Fact]
    public void DecodeEscape_Ris_IsNamed()
    {
        var decoded = EscapeSequenceDecoder.DecodeEscape(ReadOnlySpan<byte>.Empty, (byte)'c');

        Assert.True(decoded.IsKnown);
        Assert.Equal("RIS", decoded.Mnemonic);
        Assert.Equal("ESCc", decoded.Rendered);
    }

    [Fact]
    public void DecodeEscape_CharsetDesignation_ReportsSlotAndCharset()
    {
        var intermediates = new byte[] { (byte)'(' };
        var decoded = EscapeSequenceDecoder.DecodeEscape(new ReadOnlySpan<byte>(intermediates), (byte)'E');

        Assert.Equal("SCS", decoded.Mnemonic);
        Assert.Equal("G0 = Norwegian/Danish (ISO 646 NO)", decoded.Arguments);
    }

    [Fact]
    public void DecodeEscape_UnrecognisedFinal_IsFlaggedUnknown()
    {
        var decoded = EscapeSequenceDecoder.DecodeEscape(ReadOnlySpan<byte>.Empty, (byte)'q');

        Assert.False(decoded.IsKnown);
    }

    // ---- C0 controls ----

    [Fact]
    public void DecodeControl_CarriageReturn_IsNamed()
    {
        var decoded = EscapeSequenceDecoder.DecodeControl(0x0D);

        Assert.True(decoded.IsKnown);
        Assert.Equal("CR", decoded.Mnemonic);
        Assert.Equal("Carriage Return", decoded.Name);
    }

    [Fact]
    public void DecodeControl_TdvCursorCodes_MentionTheTdvMeaning()
    {
        // The TDV keyboard sends C0 codes for the arrow keys; the log must say so.
        Assert.Equal("TDV: cursor up", EscapeSequenceDecoder.DecodeControl(0x1C).Arguments);
        Assert.Equal("TDV: cursor down", EscapeSequenceDecoder.DecodeControl(0x0B).Arguments);
        Assert.Equal("TDV: cursor left", EscapeSequenceDecoder.DecodeControl(0x08).Arguments);
        Assert.Equal("TDV: cursor right", EscapeSequenceDecoder.DecodeControl(0x18).Arguments);
        Assert.Equal("TDV: home", EscapeSequenceDecoder.DecodeControl(0x1D).Arguments);
    }

    // ---- Printable run rendering ----

    [Fact]
    public void RenderPrintable_ShortRun_IsQuotedVerbatim()
    {
        var bytes = Encoding.ASCII.GetBytes("LIST-STATUS");
        var text = EscapeSequenceDecoder.RenderPrintable(new ReadOnlySpan<byte>(bytes));

        Assert.Equal("\"LIST-STATUS\"", text);
    }

    [Fact]
    public void RenderPrintable_LongRun_IsTruncatedWithACount()
    {
        // The 81-space run in the sample log must collapse, not fill the screen with "20 20 20".
        var bytes = new byte[81];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = 0x20;

        var text = EscapeSequenceDecoder.RenderPrintable(new ReadOnlySpan<byte>(bytes), maxChars: 10);

        Assert.EndsWith("(81 chars)", text);
        Assert.StartsWith("\"          \"", text);
    }

    [Fact]
    public void RenderPrintable_NonPrintableBytes_BecomeDots()
    {
        var bytes = new byte[] { 0x41, 0x00, 0x42 };
        var text = EscapeSequenceDecoder.RenderPrintable(new ReadOnlySpan<byte>(bytes));

        Assert.Equal("\"A.B\"", text);
    }
}
