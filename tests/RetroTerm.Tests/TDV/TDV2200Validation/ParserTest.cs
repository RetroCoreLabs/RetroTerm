using System.Text;
using RetroTerm.Core.Terminal.Parsing;
using Xunit;

namespace RetroTerm.Tests.TDV.TDV2200Validation;

public class ParserTest
{
    [Fact]
    public void Parser_ShouldDispatchRIS()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        bool dispatchCalled = false;
        byte finalByte = 0;

        parser.OnEscapeDispatch += (p) =>
        {
            dispatchCalled = true;
            finalByte = p.FinalByte;
        };

        // Act - Send ESC 'c' together (same as ProcessInput does)
        var risBytes = new byte[] { 0x1B, (byte)'c' };
        parser.ProcessBytes(risBytes);

        // Assert
        Assert.True(dispatchCalled, "OnEscapeDispatch should be called");
        Assert.Equal((byte)'c', finalByte);
    }

    [Fact]
    public void Parser_ShouldDispatchRIS_ByteByByte()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        bool dispatchCalled = false;
        byte finalByte = 0;

        parser.OnEscapeDispatch += (p) =>
        {
            dispatchCalled = true;
            finalByte = p.FinalByte;
        };

        // Act - Send ESC 'c' byte by byte
        parser.ProcessBytes(new byte[] { 0x1B }); // ESC
        parser.ProcessBytes(new byte[] { (byte)'c' }); // 'c'

        // Assert
        Assert.True(dispatchCalled, "OnEscapeDispatch should be called");
        Assert.Equal((byte)'c', finalByte);
    }

    [Fact]
    public void Parser_ShouldRecoverFromUtf8Sequence_WhenESCReceived()
    {
        // Arrange - Put parser in Utf8Sequence state (simulate incomplete UTF-8)
        var parser = new EscapeSequenceParser();
        bool dispatchCalled = false;
        byte finalByte = 0;

        parser.OnEscapeDispatch += (p) =>
        {
            dispatchCalled = true;
            finalByte = p.FinalByte;
        };

        // Send a UTF-8 start byte to put parser in Utf8Sequence state
        parser.ProcessBytes(new byte[] { 0xC2 }); // UTF-8 start byte (expects 1 continuation byte)
                                                  // Parser should now be in Utf8Sequence state

        // Act - Send ESC 'c' - should recover from Utf8Sequence and process ESC correctly
        parser.ProcessBytes(new byte[] { 0x1B }); // ESC - should reset to Escape state
        parser.ProcessBytes(new byte[] { (byte)'c' }); // 'c'

        // Assert
        Assert.True(dispatchCalled, "OnEscapeDispatch should be called even when starting from Utf8Sequence state");
        Assert.Equal((byte)'c', finalByte);
    }
}

