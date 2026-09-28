using System;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Core.Terminal.Input;
using Xunit;

namespace RetroTerm.Tests.Desktop;

/// <summary>
/// Tests for EscapeSequenceFormatter.Parse (escape sequence parsing)
/// and EscapeSequenceFormatter.Format (reverse formatting)
/// </summary>
public class ProgrammableKeysDialogTests
{
    [Fact]
    public void Parse_SimpleText_ReturnsByteArray()
    {
        var result = EscapeSequenceFormatter.Parse("hello");

        Assert.Equal(5, result.Length);
        Assert.Equal((byte)'h', result[0]);
        Assert.Equal((byte)'e', result[1]);
        Assert.Equal((byte)'l', result[2]);
        Assert.Equal((byte)'l', result[3]);
        Assert.Equal((byte)'o', result[4]);
    }

    [Fact]
    public void Parse_CarriageReturn_ReturnsCorrectByte()
    {
        var result = EscapeSequenceFormatter.Parse("test\\r");

        Assert.Equal(5, result.Length);
        Assert.Equal(0x0D, result[4]);
    }

    [Fact]
    public void Parse_LineFeed_ReturnsCorrectByte()
    {
        var result = EscapeSequenceFormatter.Parse("test\\n");

        Assert.Equal(5, result.Length);
        Assert.Equal(0x0A, result[4]);
    }

    [Fact]
    public void Parse_Tab_ReturnsCorrectByte()
    {
        var result = EscapeSequenceFormatter.Parse("\\ttest");

        Assert.Equal(5, result.Length);
        Assert.Equal(0x09, result[0]);
    }

    [Fact]
    public void Parse_EscapeCharacter_ReturnsCorrectByte()
    {
        var result = EscapeSequenceFormatter.Parse("\\etest");

        Assert.Equal(5, result.Length);
        Assert.Equal(0x1B, result[0]);
    }

    [Fact]
    public void Parse_HexByte_ReturnsCorrectByte()
    {
        var result = EscapeSequenceFormatter.Parse("\\x41");

        Assert.Single(result);
        Assert.Equal(0x41, result[0]);
    }

    [Fact]
    public void Parse_MultipleHexBytes_ReturnsCorrectBytes()
    {
        var result = EscapeSequenceFormatter.Parse("\\x1B\\x5B\\x41");

        Assert.Equal(3, result.Length);
        Assert.Equal(0x1B, result[0]);
        Assert.Equal(0x5B, result[1]);
        Assert.Equal(0x41, result[2]);
    }

    [Fact]
    public void Parse_EscapedBackslash_ReturnsBackslash()
    {
        var result = EscapeSequenceFormatter.Parse("test\\\\end");

        Assert.Equal(8, result.Length);
        Assert.Equal((byte)'\\', result[4]);
    }

    [Fact]
    public void Parse_ComplexSequence_ParsesCorrectly()
    {
        var result = EscapeSequenceFormatter.Parse("login user\\r\\npassword\\r");

        Assert.Equal(21, result.Length);
        Assert.Equal((byte)'l', result[0]);
        Assert.Equal(0x0D, result[10]);
        Assert.Equal(0x0A, result[11]);
        Assert.Equal(0x0D, result[20]);
    }

    [Fact]
    public void Parse_TDVFunctionKey_ParsesCorrectly()
    {
        var result = EscapeSequenceFormatter.Parse("\\x1BOP");

        Assert.Equal(3, result.Length);
        Assert.Equal(0x1B, result[0]);
        Assert.Equal((byte)'O', result[1]);
        Assert.Equal((byte)'P', result[2]);
    }

    [Fact]
    public void Parse_TDVHelpKey_ParsesCorrectly()
    {
        var result = EscapeSequenceFormatter.Parse("\\x1B[28~");

        Assert.Equal(5, result.Length);
        Assert.Equal(0x1B, result[0]);
        Assert.Equal((byte)'[', result[1]);
        Assert.Equal((byte)'2', result[2]);
        Assert.Equal((byte)'8', result[3]);
        Assert.Equal((byte)'~', result[4]);
    }

    [Fact]
    public void Parse_InvalidHexSequence_ThrowsException()
    {
        var exception = Assert.Throws<FormatException>(() => EscapeSequenceFormatter.Parse("\\xZZ"));
        Assert.Contains("Invalid hex sequence", exception.Message);
    }

    [Fact]
    public void Parse_IncompleteHexSequence_ThrowsException()
    {
        var exception = Assert.Throws<FormatException>(() => EscapeSequenceFormatter.Parse("\\x1"));
        Assert.Contains("Incomplete hex sequence", exception.Message);
    }

    [Fact]
    public void Parse_UnknownEscapeSequence_TreatsAsLiteral()
    {
        var result = EscapeSequenceFormatter.Parse("\\ztest");

        Assert.Equal(6, result.Length);
        Assert.Equal((byte)'\\', result[0]);
        Assert.Equal((byte)'z', result[1]);
    }

    [Fact]
    public void Parse_MixedContent_ParsesCorrectly()
    {
        var result = EscapeSequenceFormatter.Parse("cmd\\r\\x1B[2Jtext");

        Assert.Equal(12, result.Length);
        Assert.Equal((byte)'c', result[0]);
        Assert.Equal((byte)'m', result[1]);
        Assert.Equal((byte)'d', result[2]);
        Assert.Equal(0x0D, result[3]);
        Assert.Equal(0x1B, result[4]);
        Assert.Equal((byte)'[', result[5]);
        Assert.Equal((byte)'2', result[6]);
        Assert.Equal((byte)'J', result[7]);
        Assert.Equal((byte)'t', result[8]);
        Assert.Equal((byte)'e', result[9]);
        Assert.Equal((byte)'x', result[10]);
        Assert.Equal((byte)'t', result[11]);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyArray()
    {
        var result = EscapeSequenceFormatter.Parse("");

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_OnlyEscapes_ParsesCorrectly()
    {
        var result = EscapeSequenceFormatter.Parse("\\r\\n\\t\\e");

        Assert.Equal(4, result.Length);
        Assert.Equal(0x0D, result[0]);
        Assert.Equal(0x0A, result[1]);
        Assert.Equal(0x09, result[2]);
        Assert.Equal(0x1B, result[3]);
    }

    // --- Format tests ---

    [Fact]
    public void Format_EmptyString_ReturnsEmpty()
    {
        var result = EscapeSequenceFormatter.Format("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Format_NullString_ReturnsEmpty()
    {
        var result = EscapeSequenceFormatter.Format(null!);
        Assert.Equal("", result);
    }

    [Fact]
    public void Format_PrintableAscii_ReturnsLiteral()
    {
        var result = EscapeSequenceFormatter.Format("hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Format_CarriageReturn_ReturnsEscaped()
    {
        var result = EscapeSequenceFormatter.Format("\r");
        Assert.Equal("\\r", result);
    }

    [Fact]
    public void Format_LineFeed_ReturnsEscaped()
    {
        var result = EscapeSequenceFormatter.Format("\n");
        Assert.Equal("\\n", result);
    }

    [Fact]
    public void Format_Tab_ReturnsEscaped()
    {
        var result = EscapeSequenceFormatter.Format("\t");
        Assert.Equal("\\t", result);
    }

    [Fact]
    public void Format_Escape_ReturnsEscaped()
    {
        var result = EscapeSequenceFormatter.Format("\x1B");
        Assert.Equal("\\e", result);
    }

    [Fact]
    public void Format_Backslash_ReturnsEscaped()
    {
        var result = EscapeSequenceFormatter.Format("\\");
        Assert.Equal("\\\\", result);
    }

    [Fact]
    public void Format_NonPrintableByte_ReturnsHex()
    {
        // 0x01 (SOH) should be \x01
        var result = EscapeSequenceFormatter.Format("\x01");
        Assert.Equal("\\x01", result);
    }

    [Fact]
    public void Format_HighByte_ReturnsHex()
    {
        // 0x80 should be \x80
        var result = EscapeSequenceFormatter.Format("\x80");
        Assert.Equal("\\x80", result);
    }

    [Fact]
    public void Format_MixedContent_FormatsCorrectly()
    {
        // "cmd\r\x1B[2J"
        var input = "cmd\r\x1B[2J";
        var result = EscapeSequenceFormatter.Format(input);
        Assert.Equal("cmd\\r\\e[2J", result);
    }

    [Fact]
    public void Format_EscSequence_FormatsCorrectly()
    {
        // ESC O P (F1)
        var input = "\x1BOP";
        var result = EscapeSequenceFormatter.Format(input);
        Assert.Equal("\\eOP", result);
    }

    // --- Round-trip tests ---

    [Fact]
    public void RoundTrip_SimpleText_Matches()
    {
        var input = "hello";
        var roundTripped = EscapeSequenceFormatter.Format(EscapeSequenceFormatter.ParseToString(input));
        Assert.Equal(input, roundTripped);
    }

    [Fact]
    public void RoundTrip_EscapeSequences_Matches()
    {
        var input = "\\r\\n\\t\\e";
        var roundTripped = EscapeSequenceFormatter.Format(EscapeSequenceFormatter.ParseToString(input));
        Assert.Equal(input, roundTripped);
    }

    [Fact]
    public void RoundTrip_HexBytes_Matches()
    {
        // \x1B becomes \e in Format, so use \e for the input
        var input = "\\e[2J";
        var roundTripped = EscapeSequenceFormatter.Format(EscapeSequenceFormatter.ParseToString(input));
        Assert.Equal(input, roundTripped);
    }

    [Fact]
    public void RoundTrip_MixedContent_Matches()
    {
        var input = "login user\\r\\npassword\\r";
        var roundTripped = EscapeSequenceFormatter.Format(EscapeSequenceFormatter.ParseToString(input));
        Assert.Equal(input, roundTripped);
    }

    [Fact]
    public void RoundTrip_BackslashInText_Matches()
    {
        var input = "path\\\\file";
        var roundTripped = EscapeSequenceFormatter.Format(EscapeSequenceFormatter.ParseToString(input));
        Assert.Equal(input, roundTripped);
    }

    [Fact]
    public void RoundTrip_TDVFunctionKey_Matches()
    {
        var input = "\\eOP";
        var roundTripped = EscapeSequenceFormatter.Format(EscapeSequenceFormatter.ParseToString(input));
        Assert.Equal(input, roundTripped);
    }

    [Fact]
    public void RoundTrip_EmptyString_Matches()
    {
        var input = "";
        var roundTripped = EscapeSequenceFormatter.Format(EscapeSequenceFormatter.ParseToString(input));
        Assert.Equal(input, roundTripped);
    }

    [Fact]
    public void ParseToString_SimpleText_ReturnsString()
    {
        var result = EscapeSequenceFormatter.ParseToString("hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ParseToString_WithEscapes_ReturnsByteString()
    {
        var result = EscapeSequenceFormatter.ParseToString("test\\r");
        Assert.Equal(5, result.Length);
        Assert.Equal('\r', result[4]);
    }
}

/// <summary>
/// Tests for TDVPushKeys class functionality
/// </summary>
public class TDVPushKeysTests
{
    [Fact]
    public void ProgramKey_ValidKeyNumber_StoresSequence()
    {
        var pushKeys = new TDVPushKeys();
        var sequence = "test sequence";

        pushKeys.ProgramKey(1, sequence);

        Assert.Equal(sequence, pushKeys.GetKeySequence(1));
    }

    [Fact]
    public void ProgramKey_KeyNumberTooLow_ThrowsException()
    {
        var pushKeys = new TDVPushKeys();

        Assert.Throws<ArgumentOutOfRangeException>(() => pushKeys.ProgramKey(0, "test"));
    }

    [Fact]
    public void ProgramKey_KeyNumberTooHigh_ThrowsException()
    {
        var pushKeys = new TDVPushKeys();

        Assert.Throws<ArgumentOutOfRangeException>(() => pushKeys.ProgramKey(25, "test"));
    }

    [Fact]
    public void ProgramKey_EmptySequence_RemovesKey()
    {
        var pushKeys = new TDVPushKeys();
        pushKeys.ProgramKey(1, "test");

        pushKeys.ProgramKey(1, "");

        Assert.False(pushKeys.IsKeyProgrammed(1));
        Assert.Equal(string.Empty, pushKeys.GetKeySequence(1));
    }

    [Fact]
    public void GetKeySequence_UnprogrammedKey_ReturnsEmpty()
    {
        var pushKeys = new TDVPushKeys();

        var sequence = pushKeys.GetKeySequence(5);

        Assert.Equal(string.Empty, sequence);
    }

    [Fact]
    public void IsKeyProgrammed_ProgrammedKey_ReturnsTrue()
    {
        var pushKeys = new TDVPushKeys();
        pushKeys.ProgramKey(10, "test");

        var isProgrammed = pushKeys.IsKeyProgrammed(10);

        Assert.True(isProgrammed);
    }

    [Fact]
    public void IsKeyProgrammed_UnprogrammedKey_ReturnsFalse()
    {
        var pushKeys = new TDVPushKeys();

        var isProgrammed = pushKeys.IsKeyProgrammed(10);

        Assert.False(isProgrammed);
    }

    [Fact]
    public void ClearKey_ProgrammedKey_RemovesKey()
    {
        var pushKeys = new TDVPushKeys();
        pushKeys.ProgramKey(5, "test sequence");

        pushKeys.ClearKey(5);

        Assert.False(pushKeys.IsKeyProgrammed(5));
    }

    [Fact]
    public void Clear_AllKeys_RemovesAll()
    {
        var pushKeys = new TDVPushKeys();
        pushKeys.ProgramKey(1, "test1");
        pushKeys.ProgramKey(2, "test2");
        pushKeys.ProgramKey(3, "test3");

        pushKeys.Clear();

        Assert.Equal(0, pushKeys.ProgrammedKeyCount);
        Assert.False(pushKeys.IsKeyProgrammed(1));
        Assert.False(pushKeys.IsKeyProgrammed(2));
        Assert.False(pushKeys.IsKeyProgrammed(3));
    }

    [Fact]
    public void GetAllProgrammedKeys_MultipleKeys_ReturnsAllKeys()
    {
        var pushKeys = new TDVPushKeys();
        pushKeys.ProgramKey(1, "seq1");
        pushKeys.ProgramKey(5, "seq5");
        pushKeys.ProgramKey(24, "seq24");

        var allKeys = pushKeys.GetAllProgrammedKeys();

        Assert.Equal(3, allKeys.Count);
        Assert.Equal("seq1", allKeys[1]);
        Assert.Equal("seq5", allKeys[5]);
        Assert.Equal("seq24", allKeys[24]);
    }

    [Fact]
    public void ProgrammedKeyCount_ReturnsCorrectCount()
    {
        var pushKeys = new TDVPushKeys();
        pushKeys.ProgramKey(1, "test1");
        pushKeys.ProgramKey(2, "test2");

        var count = pushKeys.ProgrammedKeyCount;

        Assert.Equal(2, count);
    }

    [Fact]
    public void ProgramKey_OverwriteExisting_UpdatesSequence()
    {
        var pushKeys = new TDVPushKeys();
        pushKeys.ProgramKey(10, "original");

        pushKeys.ProgramKey(10, "updated");

        Assert.Equal("updated", pushKeys.GetKeySequence(10));
        Assert.Equal(1, pushKeys.ProgrammedKeyCount);
    }

    [Fact]
    public void ProgramKey_AllValidKeys_Succeeds()
    {
        var pushKeys = new TDVPushKeys();

        for (int i = 1; i <= 24; i++)
        {
            pushKeys.ProgramKey(i, $"key{i}");
        }

        Assert.Equal(24, pushKeys.ProgrammedKeyCount);
        for (int i = 1; i <= 24; i++)
        {
            Assert.True(pushKeys.IsKeyProgrammed(i));
            Assert.Equal($"key{i}", pushKeys.GetKeySequence(i));
        }
    }
}
